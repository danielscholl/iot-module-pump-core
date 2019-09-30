# Pump Module

This is a modified simulated temperature module used to test and modify different features.

### Build using Azure Pipelines

[![Build Status](https://dascholl.visualstudio.com/IoT/_apis/build/status/danielscholl.iot-module-pump-core?branchName=master)](https://dascholl.visualstudio.com/IoT/_build/latest?definitionId=57&branchName=master)

The DevOps Pipeline requires a Variable Group to be used as well as a Service Connection Endpoint

```
Required Variables
----------------------------------
IOT_HUB: The IoT Hub for the Deployment at Scale
SERVICE_ENDPOINT: ServiceEndpoint Connection Name
PUMP_VERSION:  The Version number to use for the module
WORKSPACE_ID:  Log Analytics Id used for Edge Metrics Collector
WORKSPACE_KEY: Log Analytics Key used for Edge Metrics Collector
```

### Example Message Format

```json
[
    {
        "asset": "simulator",
        "source": "pump_simulator",
        "events": [
            {
                "deviceId": "pump_simulator_01",
                "timeStamp": "2019-04-26T14:36:12.0218344Z",
                "machineTemperature": {
                    "value": 22.971214394420951,
                    "units": "degC",
                    "status": 200
                },
                "machinePressure": {
                    "value": 1.2245687284783362,
                    "units": "psig",
                    "status": 200
                },
                "ambientTemperature": {
                    "value": 21.248441741218997,
                    "units": "degC",
                    "status": 200
                },
                "ambientHumdity": {
                    "value": 26.0,
                    "units": "perc",
                    "status": 200
                }
            },
            {
                "deviceId": "pump_simulator_02",
                "timeStamp": "2019-04-26T14:36:12.0218344Z",
                "machineTemperature": {
                    "value": 22.971214394420951,
                    "units": "degC",
                    "status": 200
                },
                "machinePressure": {
                    "value": 1.2245687284783362,
                    "units": "psig",
                    "status": 200
                },
                "ambientTemperature": {
                    "value": 21.248441741218997,
                    "units": "degC",
                    "status": 200
                },
                "ambientHumdity": {
                    "value": 26.0,
                    "units": "perc",
                    "status": 200
                }
            }
        ]
    }
]
```