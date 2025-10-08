using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOSCallParsial.Models
{
    public class CallRequest
    {
        public string Name { get; set; } = default!;
        public string PhoneNumber { get; set; } = default!;
        public string Queue { get; set; } = default!;
    }
}
