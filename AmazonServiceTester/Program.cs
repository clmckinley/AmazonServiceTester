using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AmazonServiceTester
{
    class Program
    {
        public static string GetRandom()
        {
            return Math.Ceiling((DateTime.Now - new DateTime(2018, 7, 24)).TotalMilliseconds).ToString();
        }
        static async Task Main(string[] args) //this is part of C# 7.1 which is set in Project/Build/Advanced
        {
            var restClient = new HttpClient();
            var settingsPath = args.Count() > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            var curWriteCnt = 0;
            Settings settings = new Settings();
            try
            {
                var consoleBreakText = "================\n\n\n";
                while (string.IsNullOrWhiteSpace(settingsPath) || File.Exists(settingsPath) == false)
                {
                    Console.Write("Enter settings path:");
                    settingsPath = Console.ReadLine();
                }
                Console.WriteLine($"Loading Settings from {settingsPath}");
                settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsPath), new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Include});
                settings.DeviceId = settings.DeviceId ?? $"console-test{GetRandom()}";
                settings.ParticipantId = settings.ParticipantId ?? $"{settings.DeviceId}-user{GetRandom()}";

                Console.Write(consoleBreakText);

                var createAccountUrl = string.Format(settings.CreateAccountUrl, settings.ParticipantId, settings.DeviceId);
                var account = await DoGet<Account>(createAccountUrl, restClient);
                Console.WriteLine($"\t got {account}");
                Console.Write(consoleBreakText);

                var getCredentialsUrl = string.Format(settings.GetCredentialsUrl, account.participantId, account.password);
                var credentials = await DoGet<AccountCredentials>(getCredentialsUrl, restClient);
                Console.WriteLine($"\t got {credentials}");
                Console.Write(consoleBreakText);

                if (string.IsNullOrWhiteSpace(credentials.protocolURL) == false)
                {
                    var protocolBytes = await DoDownload(credentials.protocolURL, restClient);
                    //TODO:  Load the new protocol
                }
                else
                {
                    Console.WriteLine("Protocol not sent with credentials");
                }
                Console.Write(consoleBreakText);


                if (settings.WriteCount == 0)
                {
                    Console.WriteLine("Not configured to write anything to S3 so exiting");
                }
                else
                {
                    while (curWriteCnt < settings.WriteCount)
                    {
                        if(curWriteCnt > 0)
                        {
                            Console.WriteLine($"Sleeping for {settings.WriteInterval} seconds");
                            Console.Write(consoleBreakText);
                            Thread.Sleep(settings.WriteInterval * 1000); //convert the minutes into ms
                        }

                        Console.WriteLine($"Sending file {curWriteCnt}/{settings.WriteCount}");

                        if (credentials.expirationDateTime <= DateTimeOffset.UtcNow)
                        {
                            Console.WriteLine("Credentials expired so getting new");
                            credentials = await DoGet<AccountCredentials>(getCredentialsUrl, restClient);
                        }
                        else
                        {
                            Console.WriteLine("Credentials still good");
                        }
                        await PutFile(curWriteCnt, settings, credentials);
                        curWriteCnt++;
                    }
                }





            }
            catch (Exception exc)
            {
                Console.WriteLine("We got an error:" + exc.Message);
            }
            finally
            {
                restClient.Dispose();
                restClient = null;
            }
            Console.WriteLine("Press any key to exit");
            var k = Console.ReadKey();
            if (k.KeyChar == 's')
            {
                File.WriteAllText(settingsPath, JsonConvert.SerializeObject(settings));
            }
        }

        public static async Task PutFile(int cnt, Settings settings, AccountCredentials credentials)
        {
            try
            {
                AmazonS3Client s3Client = new AmazonS3Client(
                                            credentials.accessKeyId,
                                            credentials.secretAccessKey,
                                            RegionEndpoint.GetBySystemName(settings.S3Region)
                                            );
                var toPut = new PutObjectRequest()
                {
                    BucketName = settings.S3Bucket,
                    Key = $"{settings.ParticipantId}-{cnt}.txt",
                    ContentType = "text/plain",
                    ContentBody = $"Hello world {cnt} from {settings.ParticipantId} on {settings.DeviceId} at {DateTime.Now.ToShortDateString()}"
                };
                await s3Client.PutObjectAsync(toPut);
            }
            catch(Exception exc)
            {
                throw;
            }
        }
        static async Task<T> DoGet<T>(string url, HttpClient client)
        {
            var responseTxt = "";
            try
            {
                Console.WriteLine($"Getting {url}");
                responseTxt = await client.GetStringAsync(url);
                Console.WriteLine($"\tGot: {responseTxt}");
                return JsonConvert.DeserializeObject<T>(responseTxt);
            }
            catch (Exception exc)
            {
                Console.WriteLine($"Error doing Get.  msg:{exc.Message}responseTxt:{responseTxt}");
                throw;
            }
        }

        static async Task<byte[]> DoDownload(string url, HttpClient client)
        {
            byte[] responseBytes = null;
            try
            {
                Console.WriteLine($"downloading {url}");
                responseBytes = await client.GetByteArrayAsync(url);
                if (responseBytes == null || responseBytes.LongLength == 0)
                {
                    throw new Exception("Nothing downloaded");
                }
                Console.WriteLine($"\tGot: {responseBytes.LongLength} bytes");
                return responseBytes;
            }
            catch (Exception exc)
            {
                Console.WriteLine($"Error doing Download.  msg:{exc.Message}");
                throw;
            }
        }
    }

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
    public class Account
    {
        public string participantId { get; set; }
        public string password { get; set; }
    }

    public class AccountCredentials
    {
        public string accessKeyId { get; set; }
        public string secretAccessKey { get; set; }
        public string protocolURL { get; set; }
        public string expiration { get; set; }
        public string cmk { get; set; }

        public DateTimeOffset expirationDateTime
        {
            get
            {
                var rVal = DateTimeOffset.MinValue;
                if (string.IsNullOrWhiteSpace(expiration) == false && long.TryParse(expiration, out long milliseconds))
                {
                    rVal = DateTime.SpecifyKind(new DateTime(1970, 1, 1), DateTimeKind.Utc).AddMilliseconds(milliseconds);
                }
                return rVal;
            }
        }
    }
}
