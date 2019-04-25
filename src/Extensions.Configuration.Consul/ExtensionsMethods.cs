﻿using System;
using System.Collections.Generic;
using Consul;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Extensions.Configuration.Consul
{
    public static class ExtensionsMethods
    {
        public static IConfigurationBuilder AddConsul(this IConfigurationBuilder configurationBuilder, string address = "http://127.0.0.1:8500", string token = "", string folder = "", string dataCenter = "dc1")
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentNullException(nameof(address), "The address can't be empty.");

            if (!string.IsNullOrWhiteSpace(folder) && !folder.EndsWith("/"))
                throw new ArgumentException("Folder must end with \"/\".");

            return Add(configurationBuilder, new ConsulAgentConfiguration
            {
                ClientConfiguration = new ConsulClientConfiguration
                {
                    Address = new Uri(address),
                    Token = token,
                    Datacenter = dataCenter
                },
                QueryOptions = new ConsulQueryOptions
                {
                    Folder = folder
                }
            });
        }
        public static IConfigurationBuilder AddConsul(this IConfigurationBuilder configurationBuilder, ConsulClientConfiguration consulClientConfiguration, string folder = "")
        {
            if (consulClientConfiguration == null)
                throw new ArgumentNullException(nameof(consulClientConfiguration), "The agent url can't be empty.");

            if (!string.IsNullOrWhiteSpace(folder) && !folder.EndsWith("/"))
                throw new ArgumentException("Folder must end with \"/\".");

            return Add(configurationBuilder, new ConsulAgentConfiguration
            {
                ClientConfiguration = consulClientConfiguration,
                QueryOptions = new ConsulQueryOptions
                {
                    Folder = folder
                }
            });
        }

        public static IConfigurationBuilder AddConsul(this IConfigurationBuilder configurationBuilder, string[] args)
        {
            var dic = ParseCommandLineArgs(args);
            return configurationBuilder.AddConsul(new ConsulClientConfiguration
            {
                Address = new Uri(dic.GetDictionaryValue("consul-configuration-addr", "http://127.0.0.1:8500")),
                Token = dic.GetDictionaryValue("consul-configuration-token"),
                Datacenter = dic.GetDictionaryValue("consul-configuration-dc", "dc1"),
                WaitTime = TimeSpan.FromSeconds(10)
            }, dic.GetDictionaryValue("consul-configuration-folder"));
        }

        private static string GetDictionaryValue(this Dictionary<string, string> dictionary, string key, string defaultValue = "")
        {
            if (dictionary.TryGetValue(key, out var value))
                return value;
            return defaultValue;
        }

        public static IServiceCollection AddConsulConfigurationCenter(this IServiceCollection services, int blockingQueryWaitSeconds = 180)
        {
            if (blockingQueryWaitSeconds <= 0)
                throw new ArgumentException("The value of blockingQueryWaitSeconds must be greater than 0.", nameof(blockingQueryWaitSeconds));
            services.AddSingleton(new HostedServiceOptions { BlockingQueryWait = TimeSpan.FromSeconds(blockingQueryWaitSeconds) });
            services.AddSingleton<IHostedService, ConsulConfigurationHostedService>();
            return services;
        }

        private static IConfigurationBuilder Add(IConfigurationBuilder configurationBuilder, ConsulAgentConfiguration configuration)
        {
            return configurationBuilder.Add(new ConsulConfigurationSource(configuration));
        }

        internal static string TrimFolderPrefix(this string key, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || folder.Length == 0)
                return key;
            return key.Substring(folder.Length, key.Length - folder.Length);
        }

        private static Dictionary<string, string> ParseCommandLineArgs(IEnumerable<string> args)
        {
            var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var enumerator = args.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    var key1 = enumerator.Current;
                    var startIndex = 0;
                    if (key1.StartsWith("--"))
                        startIndex = 2;
                    else if (key1.StartsWith("-"))
                        startIndex = 1;
                    else if (key1.StartsWith("/"))
                    {
                        key1 = $"--{key1.Substring(1)}";
                        startIndex = 2;
                    }
                    var length = key1.IndexOf('=');
                    string index;
                    string str;
                    if (length < 0)
                    {
                        if (startIndex != 0)
                        {
                            if (startIndex != 1)
                                index = key1.Substring(startIndex);
                            else
                                continue;

                            if (enumerator.MoveNext())
                                str = enumerator.Current;
                            else
                                continue;
                        }
                        else
                            continue;
                    }
                    else
                    {
                        if (startIndex == 1)
                            throw new FormatException(key1);
                        index = key1.Substring(startIndex, length - startIndex);
                        str = key1.Substring(length + 1);
                    }
                    dictionary[index] = str;
                }
            }

            return dictionary;
        }
    }
}
