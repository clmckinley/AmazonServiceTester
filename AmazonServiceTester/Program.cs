using AmazonServiceTester.Models;
using Newtonsoft.Json;
using Sensus.DataStores.Remote;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AmazonServiceTester
{
    class Program
    {
        static async Task Main(string[] args) //this is part of C# 7.1 which is set in Project/Build/Advanced
        {
            var restClient = new HttpClient();
            var settingsPath = args.Count() > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            var curWriteCnt = 0;
            AmazonS3RemoteDataStore s3Store = null;
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
                settings.DeviceId = settings.DeviceId ?? $"console-test-{DateTime.Now.Ticks.ToString("x")}";
                settings.ParticipantId = settings.ParticipantId ?? $"{settings.DeviceId}-user{DateTime.Now.Ticks.ToString("x")}";

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
                            System.Threading.Thread.Sleep(settings.WriteInterval * 1000); //convert the minutes into ms
                        }

                        Console.WriteLine($"Sending file {curWriteCnt}/{settings.WriteCount}");
                        var utcExpiration = new DateTimeOffset(long.Parse(credentials.expiration), new TimeSpan(0));
                        if (utcExpiration <= DateTimeOffset.UtcNow)
                        {
                            Console.WriteLine("Credentials expired so getting new");
                            credentials = await DoGet<AccountCredentials>(getCredentialsUrl, restClient);
                            s3Store = null;
                        }
                        else
                        {
                            Console.WriteLine("Credentials still good");
                        }
                        s3Store = s3Store ?? GetS3Store(settings, credentials);
                        var toUploadString = $"Test upload for {account.participantId} at {DateTimeOffset.UtcNow.Ticks}";
                        await UploadToS3(toUploadString, s3Store, settings, restClient);
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
        static AmazonS3RemoteDataStore GetS3Store(Settings settings, AccountCredentials credentials)
        {
            AmazonS3RemoteDataStore s3Store = new AmazonS3RemoteDataStore();
            s3Store.Bucket = settings.S3Bucket;
            s3Store.ParticipantId = settings.ParticipantId;
            s3Store.DeviceId = settings.DeviceId;
            s3Store.Region = settings.S3Region;
            s3Store.IamAccountString = $"{credentials.accessKeyId}:{credentials.secretAccessKey}";
            return s3Store;
        }
        static async Task<bool> UploadToS3(string contents, AmazonS3RemoteDataStore s3Store, Settings settings, HttpClient client)
        {
            try
            {
                Console.WriteLine($"Writing {contents} to S3");
                var cancellationToken = new CancellationTokenSource(settings.S3CancellationTime*60*1000).Token;
                cancellationToken.ThrowIfCancellationRequested();
                await s3Store.WriteStringAsync(contents, cancellationToken);
                Console.WriteLine("\t Write finished");
                var rVal = await s3Store.GetDatumAsync(s3Store.GetDatumKey(contents), cancellationToken);
                Console.WriteLine($"\t Read {rVal}");
                if(rVal != contents)
                {
                    throw new Exception("write/read don't match");
                }

                return true;
            }
            catch(Exception exc)
            {
                Console.WriteLine("Write failed. Msg:" + exc.Message);
                return false;
            }
        }

    }
}
