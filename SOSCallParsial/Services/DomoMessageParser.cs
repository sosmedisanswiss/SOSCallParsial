using SOSCallParsial.Models;
using System.Text.RegularExpressions;

namespace SOSCallParsial.Services
{
    public class DomoMessageParser
    {
        private readonly Regex _mainRegex = new(@"ADM-CID""(?<Seq>\d{4})L0#(?<Acct>\w+)\[#\w+\|(?<Evnt>\d{4}) (?<Grp>\d{2}) (?<Zne>\d{3})\]");
        private readonly Regex _clipRegex = new(@"\[\$C00(?<Phone>\d+)\]");

        public DomoMessage? Parse(string rawMessage)
        {
            var match = _mainRegex.Match(rawMessage);
            if (!match.Success) return null;

            string? phoneNumber = null;
            var phoneMatch = _clipRegex.Match(rawMessage);
            if (phoneMatch.Success)
                phoneNumber = "+" + phoneMatch.Groups["Phone"].Value;

            return new DomoMessage
            {
                Account = match.Groups["Acct"].Value,
                EventCode = match.Groups["Evnt"].Value,
                GroupCode = match.Groups["Grp"].Value,
                ZoneCode = match.Groups["Zne"].Value,
                RawMessage = rawMessage,
                PhoneNumber = phoneNumber
            };
        }
    }
}
