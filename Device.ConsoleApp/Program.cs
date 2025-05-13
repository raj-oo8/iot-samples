using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace Device.ConsoleApp
{
    class Program
    {
        private static bool _shouldPause = false;
        private static int _pauseDuration = 0;

        static async Task Main()
        {
            var configurationBuilder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddUserSecrets<Program>();
            IConfiguration configuration = configurationBuilder.Build();
            var azureIotHubConnectionString = configuration["AzureIotHubConnectionString"];

            var deviceClient = DeviceClient.CreateFromConnectionString(azureIotHubConnectionString, TransportType.Mqtt);

            // Set up cloud-to-device message handler
            await deviceClient.SetReceiveMessageHandlerAsync(OnCloudToDeviceMessageReceived, deviceClient);

            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("Sending gradual temperature telemetry. Send a cloud-to-device message with 'pause' to pause telemetry.");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;

            double temperature = 0;
            bool increasing = true;

            while (true)
            {
                if (_shouldPause)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine();
                    Console.WriteLine($"Pausing telemetry for {_pauseDuration / 1000} seconds...");
                    await Task.Delay(_pauseDuration);
                    _shouldPause = false;
                    Console.WriteLine("Resuming telemetry...");
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.White;
                }

                // Generate telemetry
                var telemetry = new
                {
                    temperature,
                    timestamp = DateTime.UtcNow
                };

                string messageString = JsonSerializer.Serialize(telemetry);
                var message = new Message(Encoding.UTF8.GetBytes(messageString))
                {
                    ContentType = "application/json",
                    ContentEncoding = "utf-8"
                };

                await deviceClient.SendEventAsync(message);

                // Convert UTC timestamp to local timezone
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(telemetry.timestamp, TimeZoneInfo.Local);
                Console.WriteLine($"Temperature: {temperature}°C, Sent at: {localTime:h:mm:ss tt}");

                // Adjust temperature
                if (increasing)
                {
                    temperature++;
                    if (temperature >= 40)
                    {
                        increasing = false;
                    }
                }
                else
                {
                    temperature--;
                    if (temperature <= 0)
                    {
                        increasing = true;
                    }
                }

                await Task.Delay(1000); // Send every second
            }
        }

        private static async Task OnCloudToDeviceMessageReceived(Message message, object userContext)
        {
            var deviceClient = (DeviceClient)userContext;

            string messageContent = Encoding.UTF8.GetString(message.GetBytes());
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine($"Received message: {messageContent}");

            try
            {
                var command = JsonSerializer.Deserialize<CloudCommand>(messageContent);
                if (command?.Command == "pause")
                {
                    _shouldPause = true;
                    _pauseDuration = command.Duration;
                    Console.WriteLine($"Received pause command with duration: {_pauseDuration / 1000} seconds.");
                }
            }
            catch (JsonException)
            {
                Console.WriteLine("Invalid message format.");
            }
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;

            // Complete the message so it is removed from the queue
            await deviceClient.CompleteAsync(message);
        }

        private class CloudCommand
        {
            public required string Command { get; set; }
            public required int Duration { get; set; } // Duration in milliseconds
        }
    }
}
