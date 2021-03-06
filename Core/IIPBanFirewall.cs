﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IPBan
{
    public interface IIPBanFirewall
    {
        /// <summary>
        /// Ensure the firewall is initialized
        /// </summary>
        void Initialize(string rulePrefix);

        /// <summary>
        /// Creates new rules to block all the ip addresses, and removes any left-over rules. Exceptions are logged.
        /// Pass an empty list to remove all blocked ip addresses.
        /// </summary>
        /// <param name="ipAddresses">IP Addresses</param>
        /// <returns>True if success, false if error</returns>
        bool BlockIPAddresses(IReadOnlyList<string> ipAddresses);

        /// <summary>
        /// Deletes any existing rule prefixed by ruleNamePrefix then creates a new rule(s) prefixed by ruleNamePrefix with block rules for all ranges specified.
        /// </summary>
        /// <param name="ruleNamePrefix">Rule name prefix</param>
        /// <param name="ranges">Ranges to block</param>
        /// <param name="allowedPorts">Allowed ports, any port not in this list is blocked</param>
        void BlockIPAddresses(string ruleNamePrefix, IEnumerable<IPAddressRange> ranges, params PortRange[] allowedPorts);

        /// <summary>
        /// Creates new rules to allow all the ip addresses on all ports, and removes any left-over rules. Exceptions are logged.
        /// </summary>
        /// <param name="ipAddresses">IP Addresses</param>
        /// <returns>True if success, false if error</returns>
        bool AllowIPAddresses(IReadOnlyList<string> ipAddresses);

        /// <summary>
        /// Checks if an ip address is blocked in the firewall
        /// </summary>
        /// <param name="ipAddress">IP Address</param>
        /// <returns>True if the ip address is blocked in the firewall, false otherwise</returns>
        bool IsIPAddressBlocked(string ipAddress);

        /// <summary>
        /// Checks if an ip address is explicitly allowed in the firewall
        /// </summary>
        /// <param name="ipAddress">IP Address</param>
        /// <returns>True if explicitly allowed, false if not</returns>
        bool IsIPAddressAllowed(string ipAddress);

        /// <summary>
        /// Gets all banned ip addresses
        /// </summary>
        /// <returns>IEnumerable of all ip addresses</returns>
        IEnumerable<string> EnumerateBannedIPAddresses();

        /// <summary>
        /// Gets all explicitly allowed ip addresses
        /// </summary>
        /// <returns>IEnumerable of all ip addresses</returns>
        IEnumerable<string> EnumerateAllowedIPAddresses();
    }

    public static class IPBanFirewallUtility
    {
        /// <summary>
        /// Get a firewall ip address, clean and normalize
        /// </summary>
        /// <param name="ipAddress">IP Address</param>
        /// <param name="normalizedIP">The normalized ip ready to go in the firewall or null if invalid ip address</param>
        /// <returns>True if ip address can go in the firewall, false otherwise</returns>
        public static bool TryGetFirewallIPAddress(this string ipAddress, out string normalizedIP)
        {
            normalizedIP = ipAddress?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedIP) ||
                normalizedIP == "0.0.0.0" ||
                normalizedIP == "127.0.0.1" ||
                normalizedIP == "::0" ||
                normalizedIP == "::1" ||
                !IPAddressRange.TryParse(normalizedIP, out _))
            {
                normalizedIP = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Create a firewall
        /// </summary>
        /// <param name="osAndFirewall">Dictionary of string operating system name (Windows, Linux, OSX) and firewall class</param>
        /// <param name="rulePrefix">Rule prefix or null for default</param>
        /// <returns>Firewall</returns>
        public static IIPBanFirewall CreateFirewall(IReadOnlyDictionary<string, string> osAndFirewall, string rulePrefix)
        {
            bool foundFirewallType = false;
            Type firewallType = typeof(IIPBanFirewall);
            var q =
                from a in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes())
                where a != firewallType &&
                    firewallType.IsAssignableFrom(a) &&
                    a.GetCustomAttribute<RequiredOperatingSystemAttribute>() != null &&
                    a.GetCustomAttribute<RequiredOperatingSystemAttribute>().IsValid
                select a;
            foreach (Type t in q)
            {
                firewallType = t;
                CustomNameAttribute customName = t.GetCustomAttribute<CustomNameAttribute>();

                // look up the requested firewall by os name
                if (osAndFirewall.TryGetValue(IPBanOS.Name, out string firewallToUse) &&

                    // check type name or custom name attribute name, at least one must match
                    (t.Name == firewallToUse ||
                    (customName != null && (customName.Name ?? string.Empty).Equals(firewallToUse, StringComparison.OrdinalIgnoreCase))))
                {
                    foundFirewallType = true;
                    break;
                }
            }
            if (firewallType == null)
            {
                throw new ArgumentException("Firewall is null, at least one type should implement IIPBanFirewall");
            }
            else if (osAndFirewall.Count != 0 && !foundFirewallType)
            {
                string typeString = string.Join(',', osAndFirewall.Select(kv => kv.Key + ":" + kv.Value));
                throw new ArgumentException("Unable to find firewalls of types: " + typeString + ", osname: " + IPBanOS.Name);
            }
            IIPBanFirewall firewall = Activator.CreateInstance(firewallType) as IIPBanFirewall;
            firewall.Initialize(string.IsNullOrWhiteSpace(rulePrefix) ? "IPBan_" : rulePrefix);
            return firewall;
        }
    }
}
