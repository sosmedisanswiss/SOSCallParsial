using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOSCallParsial.Models.Configs
{
    public class TcpSettings
    {
        public string Host { get; set; } = default!;
        public int Port { get; set; }
        public string AllowedIp { get; set; } = default!;

    }
}   
