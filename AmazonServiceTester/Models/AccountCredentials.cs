using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AmazonServiceTester.Models
{
    public class AccountCredentials
    {
        public string accessKeyId { get; set; }
        public string secretAccessKey { get; set; }
        public string protocolURL { get; set; }
        public string expiration { get; set; }
        public string cmk { get; set; }
    }
}
