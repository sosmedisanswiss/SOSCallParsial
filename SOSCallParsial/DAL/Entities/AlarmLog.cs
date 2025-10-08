using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SOSCallParsial.DAL.Entities
{
    public class AlarmLog
    {
        public int Id { get; set; }

        public string Account { get; set; } = default!;

        public string EventCode { get; set; } = default!;

        public string GroupCode { get; set; } = default!;

        public string ZoneCode { get; set; } = default!;

        public string? PhoneNumber { get; set; }

        public DateTime Timestamp { get; set; }

        public string RawMessage { get; set; } = default!;
    }

}
