using Amazon;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AmazonServiceTester
{
    internal class Program
    {
        public static string GetRandom()
        {
            return Math.Ceiling((DateTime.Now - new DateTime(2018, 7, 24)).TotalMilliseconds).ToString();
        }

        private static async Task Main(string[] args) //this is part of C# 7.1 which is set in Project/Build/Advanced
        {
            var handler = new HttpClientHandler();
            handler.ClientCertificateOptions = ClientCertificateOption.Manual;
            handler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, cetChain, policyErrors) =>
                            {
                                if (policyErrors == System.Net.Security.SslPolicyErrors.None)
                                {
                                    return true;
                                }
                                System.Diagnostics.Trace.WriteLine(cert.GetCertHashString());
                                return true;
                            };


            var restClient = new HttpClient(handler);
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
                settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsPath), new JsonSerializerSettings() { NullValueHandling = NullValueHandling.Include });
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
                        if (curWriteCnt > 0)
                        {
                            Console.WriteLine($"Sleeping for {settings.WriteInterval} seconds");
                            Console.Write(consoleBreakText);
                            Thread.Sleep(settings.WriteInterval * 1000); //convert the minutes into ms
                        }

                        Console.WriteLine($"Sending file {curWriteCnt + 1}/{settings.WriteCount}");


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
                handler.Dispose();
                handler = null;
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
                AmazonS3Client s3Client = new AmazonS3Client(credentials.AWSCredentials,
                                            RegionEndpoint.GetBySystemName(settings.S3Region)
                                            );
                var str = $"Hello world {cnt} from {settings.ParticipantId} on {settings.DeviceId} at {DateTime.Now.ToShortDateString()}";
                var dataBytes = await EncryptData(str, credentials, settings.S3Region);
                using (MemoryStream encryptedStream = new MemoryStream(dataBytes))
                {
                    var toPut = new PutObjectRequest()
                    {
                        BucketName = settings.S3Bucket,
                        Key = $"{settings.ParticipantId}-{cnt}- byte cypher-test3.bin",
                        ContentType = "text/plain",
                        InputStream = encryptedStream
                    };
                    await s3Client.PutObjectAsync(toPut);
                }
            }
            catch (WebException exc)
            {
                var c = exc;
            }
            //catch(AmazonS3Exception exc)
            //{
            //    if(exc.ErrorCode == "InvalidAccessKeyId" || exc.ErrorCode == "SignatureDoesNotMatch" || exc.ErrorCode == "InvalidToken")
            //    {
            //        var t = "retry";
            //    }
            //}
            catch (Exception exc)
            {
                throw;
            }
        }

        private static async Task<T> DoGet<T>(string url, HttpClient client)
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

        private static async Task<byte[]> DoDownload(string url, HttpClient client)
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

        public static async Task<byte[]> EncryptData(string toEncrypt, AccountCredentials credentials, string region, CancellationToken token = default)
        {
            try
            {
                var jsonMemStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(toEncrypt ?? ""));
                var cmk = credentials.cmk;
                var kmsClient = new AmazonKeyManagementServiceClient(credentials.AWSCredentials, RegionEndpoint.GetBySystemName(region));
                kmsClient.ExceptionEvent += KmsClient_ExceptionEvent;

                var dataKeyRequest = new GenerateDataKeyRequest()
                {
                    KeyId = cmk, //"alias/console-test1846939174-user1846939660"
                    KeySpec = DataKeySpec.AES_128
                };


                GenerateDataKeyResponse dataKeyResponse = kmsClient.GenerateDataKey(dataKeyRequest);

                var plaintextKey = await StreamToByteArray(dataKeyResponse.Plaintext);

                var encryptedKey = await StreamToByteArray(dataKeyResponse.CiphertextBlob);

                var key = encryptedKey;

                var encryptedResponse = await kmsClient.EncryptAsync(new EncryptRequest()
                {
                    KeyId = cmk,
                    Plaintext = jsonMemStream,
                }, token);

                var dataBytes = await GetByteDataPackage(encryptedResponse, key);

                //var dataBytes = Encoding.ASCII.GetBytes(dataPack);
                return dataBytes;
            }
            catch (Exception exc)  //took just over 15 seconds
            {

            }
            return null;
        }

        private static void KmsClient_ExceptionEvent(object sender, ExceptionEventArgs e)
        {
            throw new NotImplementedException();
        }

        private static async Task<byte[]> GetByteDataPackage(EncryptResponse encryptResponse, byte[] key)
        {
            List<byte> rVal = new List<byte>(BitConverter.GetBytes(key.Length));
            rVal.AddRange(key);
            rVal.AddRange(await StreamToByteArray(encryptResponse.CiphertextBlob));
            return rVal.ToArray();            
        }


        private static async Task<string> GetDataPackage(EncryptResponse encryptResponse, string key = null)
        {
            key = key ?? encryptResponse.KeyId;
            StringBuilder rVal = new StringBuilder();
            rVal.Append(key.Length.ToString("000"));
            rVal.Append(key);
            rVal.Append(await StreamToString(encryptResponse.CiphertextBlob));
            return rVal.ToString();
        }

        private static async Task<byte[]> StreamToByteArray(Stream stream)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                return ms.ToArray();
            }
        }
        private static async Task<string> StreamToString(Stream stream)
        {
            return await new StreamReader(stream).ReadToEndAsync();
        }
    }

    internal class Settings
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
        public string sessionToken { get; set; }
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

        public SessionAWSCredentials AWSCredentials
        {
            get
            {
                if(accessKeyId != null && secretAccessKey != null && sessionToken != null)
                {
                    return new SessionAWSCredentials(accessKeyId, secretAccessKey, sessionToken);
                }
                return null;
            }
        }
    }
}


/*
* Encrypt the specified file using a specific CMK. This will write the
* encrypted file to the same location but with a .encrypted extension.
* Additionally the key for the file will have a .key extension.
* 
 * @param srcFilePath
*            The path of the file to encrypt
* @param cmk
*            The CMK for this user (Should be able to just use the alias, which
*            is the deviceId)
* @return The encrypted datakey used to encrypt the file

public ByteBuffer encryptData(String srcFilePath, String cmk)
{
    // Get a data key from KMS using the specified CMK
    GenerateDataKeyResult datakey = createDataKey(cmk);

    // Convert the datakey into a SecretKey for encryption
    SecretKey cryptoKey = convertDataKey(datakey.getPlaintext());

    // Create a JCE master key provider using the random key and an AES-GCM
    // encryption algorithm
    JceMasterKey masterKey = JceMasterKey.getInstance(cryptoKey, "Example", "RandomKey", "AES/GCM/NoPadding");

    // Instantiate the SDK
    AwsCrypto crypto = new AwsCrypto();

    // Create an encryption context to identify this ciphertext
    Map<String, String> context = Collections.singletonMap("Example", "FileStreaming");

    // Because the file might be to large to load into memory, we stream the data,
    // instead of loading it all at once.
    FileInputStream in;
    try
    {
                             in = new FileInputStream(srcFilePath);
        CryptoInputStream<JceMasterKey> encryptingStream = crypto.createEncryptingStream(masterKey, in, context);

        FileOutputStream out = new FileOutputStream(srcFilePath + ".encrypted");
        IOUtils.copy(encryptingStream, out);
        encryptingStream.close();
                             out.close();
    }
    catch (FileNotFoundException e)
    {
        // TODO Auto-generated catch block
        e.printStackTrace();
    }
    catch (IOException e)
    {
        // TODO Auto-generated catch block
        e.printStackTrace();
    }

    FileChannel fc;
    try
    {
        fc = new FileOutputStream(srcFilePath + ".key").getChannel();
        fc.write(datakey.getCiphertextBlob());
        fc.close();
        datakey.getCiphertextBlob().rewind();
    }
    catch (FileNotFoundException e)
    {
        // TODO Auto-generated catch block
        e.printStackTrace();
    }
    catch (IOException e)
    {
        // TODO Auto-generated catch block
        e.printStackTrace();
    }

    return datakey.getCiphertextBlob();
}
*/
