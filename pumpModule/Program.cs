namespace pumpModule
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ApplicationInsights;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Edge.ModuleUtil;
    using Microsoft.Azure.Devices.Edge.Util;
    using Microsoft.Azure.Devices.Edge.Util.Concurrency;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;

    class Program
    {
        static readonly Guid BatchId = Guid.NewGuid();
        static readonly AtomicBoolean Reset = new AtomicBoolean(false);
        static readonly Random Rnd = new Random();

        static bool sendData = true;
        static int eventCount = 1;

        static TelemetryClient telemetryClient;
        static ModuleClient moduleClient;
        static TwinCollection reportedProperties = new TwinCollection();

        static bool debug = false;
        static bool insights = false;

        static bool SendUnlimitedMessages(int maximumNumberOfMessages) => maximumNumberOfMessages < 0;

        static void Main(string[] args)
        {
            Init().Wait();
        }


        static async Task<int> Init()
        {
            Console.WriteLine("PumpModule Main() started.");
            (CancellationTokenSource cts, ManualResetEventSlim completed, Option<object> handler) = ShutdownHandler.Init(TimeSpan.FromSeconds(5), null);


            // Setup App Insights
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY")))
            {
                Console.WriteLine($"\t\t AppInsights: Enabled");
                insights = true;
                telemetryClient = new TelemetryClient();
                telemetryClient.Context.Device.Id = Environment.MachineName;
                telemetryClient.TrackEvent("Module started");
                telemetryClient.GetMetric("PumpCount").TrackValue(1);
            }

            try
            {
               
                var appSettings = SimulatorParameters.Create();
                debug = appSettings.Debug;

                Console.WriteLine(
                $"\t\t Initializing Simulator MessageCount:{(SendUnlimitedMessages(appSettings.MessageCount) ? "unlimited" : appSettings.MessageCount.ToString())}");

                moduleClient = await ModuleUtil.CreateModuleClientAsync(
                            appSettings.Protocol,
                            ModuleUtil.DefaultTimeoutErrorDetectionStrategy,
                            ModuleUtil.DefaultTransientRetryStrategy);

                await moduleClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertiesUpdated, appSettings);
                await moduleClient.SetMethodHandlerAsync("reset", ResetMethod, null);
                await moduleClient.SetMethodHandlerAsync("ping", PingMethod, null);
                await moduleClient.SetMethodHandlerAsync("checkin", CheckInMethod, null);

                await RetrieveSettingsFromTwin(appSettings);
                await SendEvents(moduleClient, appSettings, cts);

                Console.WriteLine("PumpModule Main() finished.");
                return 0;
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"{DateTime.Now.ToLocalTime()}>\t PumpSimulator Main() error.");
                Console.WriteLine(ex.Message);
                var telemetry = new ExceptionTelemetry(ex);
                telemetryClient.TrackException(telemetry);
                return -1;
            }
        }


        static async Task SendEvents(
            ModuleClient moduleClient,
            SimulatorParameters appSettings,
            CancellationTokenSource cts)
        {
            int count = 1;
            double currentTemp = appSettings.TempMin;
            double normal = (appSettings.PressureMax - appSettings.PressureMin) / (appSettings.TempMax - appSettings.TempMin);

            while (!cts.Token.IsCancellationRequested && (SendUnlimitedMessages(appSettings.MessageCount) || appSettings.MessageCount >= count))
            {
                if (Reset)
                {
                    currentTemp = appSettings.TempMin;
                    Reset.Set(false);
                }

                if (currentTemp > appSettings.TempMax)
                {
                    currentTemp += Rnd.NextDouble() - 0.5; // add value between [-0.5..0.5]
                }
                else
                {
                    currentTemp += -0.25 + (Rnd.NextDouble() * 1.5); // add value between [-0.25..1.25] - average +0.5
                }

                if (sendData)
                {
                    var events = new List<MessageEvent>();

                    var deviceId = Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
                    if (String.IsNullOrEmpty(deviceId)) deviceId = Environment.MachineName;

                    for (int i = 0; i < eventCount; i++)
                    {
                        events.Add(new MessageEvent
                        {
                            DeviceId = deviceId + "-" + i,
                            TimeStamp = DateTime.UtcNow,
                            Temperature = new SensorReading
                            {
                                Value = currentTemp,
                                Units = "degC",
                                Status = 200
                            },
                            Pressure = new SensorReading
                            {
                                Value = appSettings.PressureMin + ((currentTemp - appSettings.TempMin) * normal),
                                Units = "psig",
                                Status = 200
                            },
                            SuctionPressure = new SensorReading
                            {
                                Value = appSettings.PressureMin + 4 + ((currentTemp - appSettings.TempMin) * normal),
                                Units = "psig",
                                Status = 200
                            },
                            DischargePressure = new SensorReading
                            {
                                Value = appSettings.PressureMin + 1 + ((currentTemp - appSettings.TempMin) * normal),
                                Units = "psig",
                                Status = 200
                            },
                            Flow = new SensorReading
                            {
                                Value = Rnd.Next(78, 82),
                                Units = "perc",
                                Status = 200
                            }
                        });
                        currentTemp += -0.25 + (Rnd.NextDouble() * 1.5);
                    }

                    var msgBody = new MessageBody
                    {
                        Asset = Environment.GetEnvironmentVariable("ASSET") ?? "simulator",
                        Source = Environment.MachineName,
                        Events = events
                    };

                    string dataBuffer = JsonConvert.SerializeObject(msgBody);
                    var eventMessage = new Message(Encoding.UTF8.GetBytes(dataBuffer));
                    eventMessage.Properties.Add("sequenceNumber", count.ToString());
                    eventMessage.Properties.Add("batchId", BatchId.ToString());
                    eventMessage.Properties.Add("asset", msgBody.Asset);
                    var size = eventMessage.GetBytes().Length;

                    if (debug) Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Size: {size}, Body: [{dataBuffer}]");
                    else Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Size: {size}");

                    try
                    {
                        if (insights)
                        {
                            telemetryClient.GetMetric("SendMessage").TrackValue(1);
                            telemetryClient.Context.Operation.Name = "Special Operation";
                            Metric sizeStats = telemetryClient.GetMetric("Special Operation Message Size");
                            sizeStats.TrackValue(size);
                        }
                        await moduleClient.SendEventAsync("temperatureOutput", eventMessage);
                    }
                    catch (Microsoft.Azure.Devices.Client.Exceptions.MessageTooLargeException exception)
                    {
                        Console.WriteLine(exception.Message);
                        if (insights)
                        {
                            telemetryClient.GetMetric("MessageSizeExceeded").TrackValue(1);
                        }
                    }
                    count++;
                }

                await Task.Delay(appSettings.MessageDelay, cts.Token);
            }

            if (appSettings.MessageCount < count)
            {
                Console.WriteLine($"Done sending {appSettings.MessageCount} messages");
            }
        }


        private static async Task RetrieveSettingsFromTwin(SimulatorParameters appSettings)
        {
            Twin currentTwinProperties = await moduleClient.GetTwinAsync();
            Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Initial Twin State: Received");

            if (currentTwinProperties.Properties.Desired.Contains(SimulatorParameters.SendIntervalConfigKey))
            {
                var desiredInterval = (int)currentTwinProperties.Properties.Desired[SimulatorParameters.SendIntervalConfigKey];
                Console.WriteLine($"\t\t SendInterval: {desiredInterval.ToString()}");
                appSettings.MessageDelay = TimeSpan.FromMilliseconds(desiredInterval);
            }

            if (currentTwinProperties.Properties.Desired.Contains(SimulatorParameters.EventCountConfigKey))
            {
                var desiredCount = (int)currentTwinProperties.Properties.Desired[SimulatorParameters.EventCountConfigKey];
                Console.WriteLine($"\t\t EventCount: {desiredCount.ToString()}");
                eventCount = desiredCount;
            }

            if (currentTwinProperties.Properties.Desired.Contains(SimulatorParameters.DebugConfigKey))
            {
                var desiredDebug = (bool)currentTwinProperties.Properties.Desired[SimulatorParameters.DebugConfigKey];
                Console.WriteLine($"\t\t Debug: {desiredDebug.ToString()}");
                debug = desiredDebug;
            }

            if (currentTwinProperties.Properties.Desired.Contains(SimulatorParameters.SendDataConfigKey))
            {
                sendData = (bool)currentTwinProperties.Properties.Desired[SimulatorParameters.SendDataConfigKey];
                if (!sendData)
                {
                    Console.WriteLine($"\t\t SendData: {sendData.ToString()}");
                }
            }
        }

        static Task<MethodResponse> PingMethod(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Direct Method: PingMethod");

            var response = new MethodResponse((int)System.Net.HttpStatusCode.OK);
            return Task.FromResult(response);
        }

        static Task<MethodResponse> ResetMethod(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Direct Method: ResetMethod");
            Reset.Set(true);

            var response = new MethodResponse((int)System.Net.HttpStatusCode.OK);
            return Task.FromResult(response);
        }

        static Task<MethodResponse> CheckInMethod(MethodRequest methodRequest, object userContext)
        {
            var currentTime = DateTime.Now.ToLocalTime();
            Console.WriteLine($"\t{currentTime}> Direct Method: CheckInMethod");

            try
            {
                var directMethod = new TwinCollection();
                directMethod["CheckTime"] = DateTime.Now;
                reportedProperties["DirectMethod"] = directMethod;

                moduleClient.UpdateReportedPropertiesAsync(reportedProperties).Wait();
                Console.WriteLine($"\t{currentTime}> Reported Properties: Sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            var response = new MethodResponse((int)System.Net.HttpStatusCode.OK);
            return Task.FromResult(response);
        }


        static async Task OnDesiredPropertiesUpdated(TwinCollection desiredProperties, object userContext)
        {
            var currentTime = DateTime.Now.ToLocalTime();
            Console.WriteLine($"\t{currentTime}> Desired Properties: Received");
            var appSettings = (SimulatorParameters)userContext;

            if (desiredProperties.Contains(SimulatorParameters.SendIntervalConfigKey))
            {
                var desiredInterval = (int)desiredProperties[SimulatorParameters.SendIntervalConfigKey];
                Console.WriteLine($"\t\t SendInterval: {desiredInterval.ToString()}");

                appSettings.MessageDelay = TimeSpan.FromMilliseconds((int)desiredProperties[SimulatorParameters.SendIntervalConfigKey]);
            }

            if (desiredProperties.Contains(SimulatorParameters.EventCountConfigKey))
            {
                var desiredCount = (int)desiredProperties[SimulatorParameters.EventCountConfigKey];
                Console.WriteLine($"\t\t EventCount: {desiredCount.ToString()}");
                eventCount = (int)desiredProperties[SimulatorParameters.EventCountConfigKey];
            }

            if (desiredProperties.Contains(SimulatorParameters.DebugConfigKey))
            {
                debug = (bool)desiredProperties[SimulatorParameters.DebugConfigKey];
                Console.WriteLine($"\t\t Debug: {debug.ToString()}");
            }

            if (desiredProperties.Contains(SimulatorParameters.SendDataConfigKey))
            {
                bool desiredSendDataValue = (bool)desiredProperties[SimulatorParameters.SendDataConfigKey];
                if (desiredSendDataValue != sendData && !desiredSendDataValue)
                {
                    Console.WriteLine($"\t\t SendData: {desiredSendDataValue.ToString()}");
                }

                sendData = desiredSendDataValue;
            }

            var settings = new TwinCollection();
            settings["SendData"] = sendData;
            settings["SendInterval"] = appSettings.MessageDelay.TotalMilliseconds;
            settings["EventCount"] = eventCount;
            reportedProperties["settings"] = settings;
            await moduleClient.UpdateReportedPropertiesAsync(reportedProperties);
        }

    }
}
