﻿using System.IO;
using Extensions.Configuration.Consul;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace Example
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>

            WebHost.CreateDefaultBuilder(args).ConfigureAppConfiguration((context, config) =>
            {
                //config.SetBasePath(Directory.GetCurrentDirectory());
                //config.AddJsonFile("appsettings.json", false, true);
                //config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", false, true);
                var configurationRoot = config.Build();
                var address = configurationRoot["ConfigurationConsul:Address"];
                var folder = configurationRoot["ConfigurationConsul:Folder"];
                var token = configurationRoot["ConfigurationConsul:Token"];
                var datacenter = configurationRoot["ConfigurationConsul:Datacenter"];
                config.AddConsul(address, token, folder, datacenter);
                //config.AddConsul(args);
                config.AddCommandLine(args);
            }).UseStartup<Startup>();
    }
}
