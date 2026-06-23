namespace Harness.Core.Modules;

public enum DeviceType
{
    Light,
    HVAC,
    Appliance
}

public record DeviceState
{
    public required DeviceType Type { get; init; }
    public bool IsOn { get; set; }

    public double PowerDraw { get; init; } // kW when on

    // HVAC
    public double CoolingPower { get; init; } // kW of cooling (positive)
    public double HeatingPower { get; init; } // kW of heating (positive)
    public double TargetTemperature { get; set; }
}

public record RoomState
{
    public required string Id { get; init; }
    public double Temperature { get; set; } // °C
    public double Humidity { get; set; } // 0-100%
    public double AirQuality { get; set; } // 0-100, higher is better
    public bool IsOccupied { get; set; }
    public List<DeviceState> Devices { get; } = [];
    public static double AirMassEstimate() => 50.0;
}

public class SimulatedHome
{
    private readonly List<RoomState> _rooms = [];
    private readonly Random _rng;
    private double _totalEnergyConsumed; // kWh
    private double _outdoorTemp = 20.0;
    private double _outdoorHumidity = 50.0;

    private double NextEventTime { get; set; } = double.PositiveInfinity;

    public const double TimeStepHours = 1.0 / 60; // 1 minute per step by default

    public SimulatedHome(int seed = 42)
    {
        _rng = new Random(seed);
        InitializeRoomsAndDevices();
    }

    private void InitializeRoomsAndDevices()
    {
        var livingRoom = new RoomState
            { Id = "living", Temperature = 22.0, Humidity = 45.0, AirQuality = 90.0, IsOccupied = false };
        var kitchen = new RoomState
            { Id = "kitchen", Temperature = 23.0, Humidity = 50.0, AirQuality = 85.0, IsOccupied = false };
        var bedroom = new RoomState
            { Id = "bedroom", Temperature = 21.0, Humidity = 48.0, AirQuality = 92.0, IsOccupied = true };
        var bathroom = new RoomState
            { Id = "bathroom", Temperature = 24.0, Humidity = 60.0, AirQuality = 80.0, IsOccupied = false };

        livingRoom.Devices.Add(new DeviceState
            { Type = DeviceType.Light, PowerDraw = 0.06 });
        livingRoom.Devices.Add(new DeviceState
        {
            Type = DeviceType.HVAC, PowerDraw = 2.5,
            CoolingPower = 3.5, HeatingPower = 3.0, TargetTemperature = 22.0
        });
        kitchen.Devices.Add(new DeviceState
            { Type = DeviceType.Light, PowerDraw = 0.08 });
        kitchen.Devices.Add(new DeviceState
            { Type = DeviceType.Appliance, PowerDraw = 1.2 });
        bedroom.Devices.Add(new DeviceState
            { Type = DeviceType.Light, PowerDraw = 0.05 });
        bedroom.Devices.Add(new DeviceState
        {
            Type = DeviceType.HVAC, PowerDraw = 2.0,
            CoolingPower = 2.8, HeatingPower = 2.5, TargetTemperature = 21.0
        });
        bathroom.Devices.Add(new DeviceState
            { Type = DeviceType.Light, PowerDraw = 0.04 });

        _rooms.AddRange([livingRoom, kitchen, bedroom, bathroom]);
        _totalEnergyConsumed = 0;

        ScheduleNextEvent();
    }

    private readonly List<Action> _pendingActions = [];

    public void ApplyAction(string command, Dictionary<string, object> parameters)
    {
        _pendingActions.Add(() => ExecuteCommand(command, parameters));
    }

    private void ExecuteCommand(string command, Dictionary<string, object> parameters)
    {
        switch (command)
        {
            case "SetSensorValue":
                break;
            case "SendMotorCommand":
                var cmd = parameters["command"] as string;
                var prms = parameters["parameters"] as double[];
                HandleMotorCommand(cmd, prms);
                break;
        }
    }

    private void HandleMotorCommand(string? command, double[]? prms)
    {
        if (command == null) return;
        switch (command.ToLower())
        {
            case "set_light":
                if (prms?.Length >= 2)
                {
                    var roomId = GetRoomIdFromIndex((int)prms[0]);
                    var on = prms[1] > 0.5;
                    SetDeviceState(roomId, DeviceType.Light, on);
                }

                break;
            case "set_hvac":
                if (prms?.Length >= 2)
                {
                    var roomId = GetRoomIdFromIndex((int)prms[0]);
                    var targetTemp = prms[1];
                    SetHvacTarget(roomId, targetTemp);
                }

                break;
            case "set_appliance":
                if (prms?.Length >= 2)
                {
                    var roomId = GetRoomIdFromIndex((int)prms[0]);
                    var on = prms[1] > 0.5;
                    SetDeviceState(roomId, DeviceType.Appliance, on);
                }

                break;
        }
    }

    private string GetRoomIdFromIndex(int index) => _rooms[Math.Clamp(index, 0, _rooms.Count - 1)].Id;

    private void SetDeviceState(string roomId, DeviceType type, bool on)
    {
        foreach (var room in _rooms.Where(r => r.Id == roomId))
        foreach (var device in room.Devices.Where(d => d.Type == type))
            device.IsOn = on;
    }

    private void SetHvacTarget(string roomId, double target)
    {
        foreach (var room in _rooms.Where(r => r.Id == roomId))
        foreach (var hvac in room.Devices.Where(d => d.Type == DeviceType.HVAC))
            hvac.TargetTemperature = Math.Clamp(target, 16, 30);
    }

    public void Update(double deltaHours)
    {
        foreach (var action in _pendingActions) action();
        _pendingActions.Clear();

        _outdoorTemp += (_rng.NextDouble() - 0.5) * 0.2;
        _outdoorTemp = Math.Clamp(_outdoorTemp, -5, 40);
        _outdoorHumidity += (_rng.NextDouble() - 0.5) * 1.0;
        _outdoorHumidity = Math.Clamp(_outdoorHumidity, 10, 95);

        foreach (var room in _rooms)
        {
            var heatTransfer = (2.0 / 60.0) * deltaHours * (_outdoorTemp - room.Temperature);
            double hvacEffect = 0;
            foreach (var device in room.Devices.Where(d => d is { Type: DeviceType.HVAC, IsOn: true }))
            {
                var diff = device.TargetTemperature - room.Temperature;
                if (diff > 0)
                {
                    hvacEffect += Math.Min(device.HeatingPower * deltaHours / (RoomState.AirMassEstimate()),
                        diff * 0.3);
                }
                else
                {
                    hvacEffect += Math.Max(-device.CoolingPower * deltaHours / (RoomState.AirMassEstimate()),
                        diff * 0.3);
                }
            }

            double internalHeat = 0;
            if (room.IsOccupied) internalHeat += 0.1 * deltaHours;
            internalHeat += room.Devices.Where(d => d is { Type: DeviceType.Appliance, IsOn: true })
                .Sum(device => device.PowerDraw * 0.8 * deltaHours / RoomState.AirMassEstimate());

            room.Temperature += heatTransfer + hvacEffect + internalHeat;
            room.Temperature = Math.Clamp(room.Temperature, -10, 50);

            room.Humidity += 0.1 * deltaHours * (_outdoorHumidity - room.Humidity);
            room.Humidity = Math.Clamp(room.Humidity, 10, 90);

            var aqDecay = room.Devices.Where(d => d is { Type: DeviceType.Appliance, IsOn: true })
                .Sum(_ => 5.0 * deltaHours);
            room.AirQuality = Math.Clamp(room.AirQuality + 10.0 * deltaHours - aqDecay, 0, 100);
        }

        var stepEnergy = _rooms.SelectMany(room => room.Devices.Where(d => d.IsOn))
            .Sum(device => device.PowerDraw * deltaHours);
        _totalEnergyConsumed += stepEnergy;

        HandleEvents(deltaHours);
    }

    private void HandleEvents(double deltaHours)
    {
        if (NextEventTime <= 0)
        {
            var eventType = _rng.Next(3);
            switch (eventType)
            {
                case 0:
                    var device = _rooms.SelectMany(r => r.Devices).OrderBy(_ => _rng.Next()).FirstOrDefault();
                    device?.IsOn = false;
                    break;
                case 1:
                    _outdoorTemp += 10;
                    break;
                case 2:
                    var room = _rooms[_rng.Next(_rooms.Count)];
                    room.IsOccupied = true;
                    break;
            }

            ScheduleNextEvent();
        }

        NextEventTime -= deltaHours;
    }

    private void ScheduleNextEvent()
    {
        NextEventTime = 0.5 + _rng.NextDouble() * 1.5; // hours
    }

    public PerceptionRewardState GetCurrentState()
    {
        return new PerceptionRewardState
        {
            Rooms = _rooms.Select(r => new RoomSnapshot
            {
                Temperature = r.Temperature,
                Humidity = r.Humidity,
                AirQuality = r.AirQuality,
            }).ToList(),
            TotalEnergy = _totalEnergyConsumed
        };
    }
}

public record RoomSnapshot
{
    public double Temperature { get; init; }
    public double Humidity { get; init; }
    public double AirQuality { get; init; }
}

public record PerceptionRewardState
{
    public List<RoomSnapshot> Rooms { get; init; } = [];
    public double TotalEnergy { get; init; }
}