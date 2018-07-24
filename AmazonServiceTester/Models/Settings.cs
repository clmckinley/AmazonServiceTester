using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AmazonServiceTester.Models
{
    class Settings
    {
        public string CreateAccountUrl { get; set; }
        public string GetCredentialsUrl { get; set; }
        public int WriteInterval { get; set; } = 1; //write every 1 minute by default
        public int WriteCount { get; set; } = 15;
        public string DeviceId { get; set; }
        public string ParticipantId { get; set; }
        public string S3Region { get; set; } = "us-east-1";
        public string S3Bucket { get; set; } = "bucketId";
        public int S3CancellationTime { get; set; } = 60;
    }
}
