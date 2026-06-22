using Google.Protobuf.WellKnownTypes;
using Harness.Abstractions;
using Harness.Abstractions.Actr;
using Harness.Abstractions.Actr.Services;

namespace Harness.Core.Modules
{
    public class PerceptionMotorModule(FrostpunkWorld.FrostpunkWorldClient client) : IModule
    {
        private ResourceState? _resourceState;
        private PopulationState? _populationState;
        private SocialState? _socialState;
        private TemperatureState? _temperatureState;
        private LawsState? _lawsState;
        private ResearchState? _researchState;
        private BuildingsState? _buildingsState;

        private bool _latestDone;
        private double _latestReward;

        public string ModuleId => "PerceptionMotor";

        public void OperateBuffer(BufferOperation op)
        {
            switch (op.Command)
            {
                case "observeResources": ObserveResources(); break;
                case "observePopulation": ObservePopulation(); break;
                case "observeSocial": ObserveSocial(); break;
                case "observeTemperature": ObserveTemperature(); break;
                case "observeLaws": ObserveLaws(); break;
                case "observeResearch": ObserveResearch(); break;
                case "observeBuildings": ObserveBuildings(); break;
                case "observeAll": ObserveAll(); break;
                case "act": Act(op.Params); break;
                case "reset": Reset(op.Params); break;
                default:
                    throw new InvalidOperationException(
                        $"PerceptionMotor does not support command '{op.Command}'.");
            }
        }

        private void ObserveResources() =>
            _resourceState = client.ObserveResources(new ObserveResourcesRequest());

        private void ObservePopulation() =>
            _populationState = client.ObservePopulation(new ObservePopulationRequest());

        private void ObserveSocial() =>
            _socialState = client.ObserveSocial(new ObserveSocialRequest());

        private void ObserveTemperature() =>
            _temperatureState = client.ObserveTemperature(new ObserveTemperatureRequest());

        private void ObserveLaws() =>
            _lawsState = client.ObserveLaws(new ObserveLawsRequest());

        private void ObserveResearch() =>
            _researchState = client.ObserveResearch(new ObserveResearchRequest());

        private void ObserveBuildings() =>
            _buildingsState = client.ObserveBuildings(new ObserveBuildingsRequest());

        private void ObserveAll()
        {
            ObserveResources();
            ObservePopulation();
            ObserveSocial();
            ObserveTemperature();
            ObserveLaws();
            ObserveResearch();
            ObserveBuildings();
        }

        private void Act(Struct parameters)
        {
            var json = parameters.ToString();
            var action = FrostpunkAction.Parser.ParseJson(json);
            var response = client.PerformAction(new PerformActionRequest { Action = action });

            if (response.NewState != null)
                UpdateCacheFromFullState(response.NewState);

            _latestDone = response.Done;
            _latestReward = response.Reward;
        }

        private void Reset(Struct parameters)
        {
            var request = new ResetRequest();
            if (parameters.Fields.TryGetValue("scenario", out var scenarioValue) &&
                scenarioValue.KindCase == Value.KindOneofCase.StringValue)
                request.Scenario = scenarioValue.StringValue;

            var state = client.Reset(request);
            UpdateCacheFromFullState(state);
            _latestDone = false;
            _latestReward = 0;
        }

        private void UpdateCacheFromFullState(FrostpunkState s)
        {
            _resourceState = new ResourceState
            {
                Day = s.Day,
                TimeOfDay = s.TimeOfDay
            };
            foreach (var r in s.Resources) _resourceState.Resources.Add(r.Key, r.Value);

            _populationState = s.Population;
            _socialState = s.Social;
            _temperatureState = s.Temperature;
            _lawsState = s.Laws;
            _researchState = s.Research;
            _buildingsState = s.Buildings;
        }

        public BufferState GetBufferState()
        {
            var full = new FrostpunkState();

            if (_resourceState != null)
            {
                full.Day = _resourceState.Day;
                full.TimeOfDay = _resourceState.TimeOfDay;
                foreach (var r in _resourceState.Resources) full.Resources.Add(r.Key, r.Value);
            }

            full.Population = _populationState;
            full.Social = _socialState;
            full.Temperature = _temperatureState;
            full.Laws = _lawsState;
            full.Research = _researchState;
            full.Buildings = _buildingsState;

            var data = Struct.Parser.ParseJson(full.ToString());
            data.Fields["_reward"] = Value.ForNumber(_latestReward);
            data.Fields["_done"] = Value.ForBool(_latestDone);

            return new BufferState { ModuleId = ModuleId, Data = data };
        }

        public ModuleSchema GetOperationSchema()
        {
            var schema = new ModuleSchema
            {
                ModuleId = ModuleId,
                CommandSchemas =
                {
                    ["observeResources"] = "{ }",
                    ["observePopulation"] = "{ }",
                    ["observeSocial"] = "{ }",
                    ["observeTemperature"] = "{ }",
                    ["observeLaws"] = "{ }",
                    ["observeResearch"] = "{ }",
                    ["observeBuildings"] = "{ }",
                    ["observeAll"] = "{ }",
                    ["act"] =
                        """
                        {
                            "type": "object",
                            "description": "FrostpunkAction JSON, e.g. { "build\": { "building_type": "tent", ... } }"
                        }
                        """,
                    ["reset"] =
                        """
                        {
                            "scenario": "string"
                        }
                        """
                }
            };
            return schema;
        }
    }
}