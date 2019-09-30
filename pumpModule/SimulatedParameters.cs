using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;

namespace pumpModule
{
    class SimulatorParameters
    {
        public static string MessageCountConfigKey = "MessageCount";
        public static  string SendDataConfigKey = "SendData";
        public static string SendIntervalConfigKey = "SendInterval";
        public static string EventCountConfigKey = "EventCount";
        public static string ProtocolConfigKey = "Protocol";
        public static string DebugConfigKey = "Debug";

        public TimeSpan MessageDelay { get; set; }

        public int MessageCount { get; set; }

        public double TempMin { get; set; }

        public double TempMax { get; set; }

        public double PressureMin { get; set; }

        public double PressureMax { get; set; }

        public double AmbientTemp { get; set; }

        public int HumidityPercent { get; set; }

        public TransportType Protocol { get; set; }

        public Boolean Debug { get; set; }

        public static SimulatorParameters Create()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            IConfigurationRoot configuration = builder.Build();

            int messageValue;
            if (!int.TryParse(Environment.GetEnvironmentVariable(MessageCountConfigKey), out messageValue))
            {
                messageValue = configuration.GetValue<Int32>("MessageCount", 500);
            }

            // MQTT=1  AMPQ=3
            int protocolValue;
            if (!int.TryParse(Environment.GetEnvironmentVariable(ProtocolConfigKey), out protocolValue))
            {
                protocolValue = configuration.GetValue<Int32>("Protocol", (int)TransportType.Mqtt_Tcp_Only);
            }

            bool debugValue;
            if (!Boolean.TryParse(Environment.GetEnvironmentVariable("DEBUG"), out debugValue))
            {
                debugValue = configuration.GetValue<Boolean>("Debug", false); ;
            }


            return new SimulatorParameters
            {
                MessageDelay = configuration.GetValue<TimeSpan>("MessageDelay", TimeSpan.FromSeconds(1000)),
                MessageCount = messageValue,
                TempMin = configuration.GetValue<Int32>("machineTempMin", 21),
                TempMax = configuration.GetValue<Int32>("machineTempMax", 100),
                PressureMin = configuration.GetValue<Int32>("machinePressureMin", 1),
                PressureMax = configuration.GetValue<Int32>("machinePressureMax", 10),
                AmbientTemp = configuration.GetValue<Int32>("ambientTemperature", 21),
                HumidityPercent = configuration.GetValue<Int32>("ambientHumidity", 25),
                Protocol = (TransportType)Enum.ToObject(typeof(TransportType), protocolValue),
                Debug = debugValue
            };
        }
    }
}
