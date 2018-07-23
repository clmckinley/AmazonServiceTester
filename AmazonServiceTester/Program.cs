using AmazonServiceTester.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AmazonServiceTester
{
    class Program
    {
        static async Task Main(string[] args) //this is part of C# 7.1 which is set in Project/Build/Advanced
        {
            var restClient = new HttpClient();
            var settingsPath = args.Count() > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            Settings settings = new Settings();
            try
            {
                var consoleBreakText = "================\n\n\n";
                var deviceId = $"console-test-{DateTime.Now.Ticks.ToString("x")}";
                var participantId = $"{deviceId}-user{DateTime.Now.Ticks.ToString("x")}";
                while (string.IsNullOrWhiteSpace(settingsPath) || File.Exists(settingsPath) == false)
                {
                    Console.Write("Enter settings path:");
                    settingsPath = Console.ReadLine();
                }
                Console.WriteLine($"Loading Settings from {settingsPath}");
                settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsPath));
                Console.Write(consoleBreakText);

                var createAccountUrl = string.Format(settings.CreateAccountUrl, participantId, deviceId);
                var account = await DoGet<Account>(createAccountUrl, restClient);
                Console.WriteLine($"\t got {account}");
                Console.Write(consoleBreakText);

                var getCredentialsUrl = string.Format(settings.GetCredentialsUrl, account.participantId, account.password);
                var credentials = await DoGet<AccountCredentials>(getCredentialsUrl, restClient);
                Console.WriteLine($"\t got {credentials}");
                Console.Write(consoleBreakText);


            }
            catch(Exception exc)
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
            if(k.KeyChar == 's')
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
            catch(Exception exc)
            {
                Console.WriteLine($"Error doing Get.  msg:{exc.Message}responseTxt:{responseTxt}");
                throw;
            }
        }
    }
}
