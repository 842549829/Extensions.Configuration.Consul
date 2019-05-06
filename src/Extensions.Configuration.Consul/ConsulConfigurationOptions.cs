using System;
using System.Collections.Generic;
using System.Text;

namespace Extensions.Configuration.Consul
{
    public class ConsulConfigurationOptions
    {
        public string Address { get; set; }

        public string RootFolder { get; set; }

        public string[] Folders { get; set; }

        public string Token { get; set; }

        public string Datacenter { get; set; }
    }
}
